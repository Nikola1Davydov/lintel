using System.Linq;
using Xunit;

namespace Lintel.Tests;

/// <summary>The checks against the IFC schema: what triggers each, and the fix where there is one.</summary>
public class SchemaDiagnosticsTests
{
    private const string Rule = "rule \"x\"\n  for IfcWall\n  ";

    private static CheckResult CheckLine(string line) => LintelCompiler.Check(TestSupport.WithHeader(Rule + line));

    private static CheckResult CheckRule(string forLine, string requirement = "require attribute Name", string header = "ifc IFC4") =>
        LintelCompiler.Check($"{header}\n\nrule \"x\"\n  {forLine}\n  {requirement}");

    [Theory]
    [InlineData("require property Pset_WallCommon.FireRatin", "E404", "require property Pset_WallCommon.FireRating")]
    [InlineData("require property Pset_WalCommon.FireRating", "E403", "require property Pset_WallCommon.FireRating")]
    [InlineData("require attribute Nam", "E402", "require attribute Name")]
    [InlineData("require attribute name", "E402", "require attribute Name")]
    [InlineData("require property A.B datatype IfcLable = \"x\"", "E406", "require property A.B datatype IfcLabel = \"x\"")]
    [InlineData("require property A.B datatype IFCLABEL", "E406", "require property A.B datatype IfcLabel")]
    [InlineData("require property Pset_WallCommon.FireRating datatype IfcText", "E407", "require property Pset_WallCommon.FireRating datatype IfcLabel")]
    [InlineData("require property Pset_WallCommon.LoadBearing = \"true\"", "E408", "require property Pset_WallCommon.LoadBearing = true")]
    [InlineData("require property Pset_WallCommon.LoadBearing = \"TRUE\"", "E408", "require property Pset_WallCommon.LoadBearing = true")]
    [InlineData("require property Pset_WallCommon.FireRating = 30", "E408", "require property Pset_WallCommon.FireRating = \"30\"")]
    [InlineData("require property Pset_WallCommon.FireRating in [30, 60]", "E408", "require property Pset_WallCommon.FireRating in [\"30\", \"60\"]")]
    [InlineData("require property A.B datatype IfcInteger = \"5\"", "E408", "require property A.B datatype IfcInteger = 5")]
    [InlineData("require property Qto_WallBaseQuantities.Width >= 150 MM", "E410", "require property Qto_WallBaseQuantities.Width >= 150 mm")]
    [InlineData("require property Pset_WallCommon.Status = \"New\"", "E408", "require property Pset_WallCommon.Status = \"NEW\"")]
    public void A_wrong_name_or_value_is_reported_with_its_fix(string line, string code, string fixedLine)
    {
        var result = CheckLine(line);

        Assert.Contains(code, result.Codes());
        Assert.Equal(TestSupport.WithHeader(Rule + fixedLine), result.Fixed);
        Assert.True(result.FixedOk, result.Describe());
    }

    [Theory]
    [InlineData("require property Pset_DoorCommon.FireRating", "E411")]
    [InlineData("require property Pset_MyOwn.Thing", "E403")]
    [InlineData("require attribute LongName", "E402")]
    [InlineData("require attribute FireRating", "E402")]
    [InlineData("require property Pset_WallCommon.ThermalTransmittance <= 0.24", "E409")]
    [InlineData("require property Qto_WallBaseQuantities.Width >= 150", "E409")]
    [InlineData("require property Qto_WallBaseQuantities.Width >= 150 kg", "E410")]
    [InlineData("require property Pset_WallCommon.FireRating = \"F90\" and length <= 3", "E201")]
    [InlineData("require property Pset_WallCommon.FireRating > 30", "E408")]
    [InlineData("require property Pset_WallCommon.LoadBearing in [true, false]", "E408")]
    [InlineData("require property A.B >= 5 mm", "E413")]
    [InlineData("require property A.B = 5 m", "E413")]
    [InlineData("require property A.B datatype IfcInteger = 5.0", "E408")]
    [InlineData("require property A.B datatype IfcReal = 5 m", "E410")]
    [InlineData("require property A.B datatype IfcLabel = 5 m", "E408")]
    [InlineData("require entity IfcSlab", "E414")]
    [InlineData("require entity only IfcWallStandardCase type FLOOR", "E405")]
    [InlineData("require part of IfcWall via IfcRelAssignsToGroup", "E415")]
    [InlineData("require part of IfcSlab via IfcRelContainedInSpatialStructure", "E415")]
    [InlineData("require attribute ConnectedTo", "E402")]
    [InlineData("require attribute OwnerHistory = \"x\"", "E408")]
    [InlineData("require attribute Name = 5", "E408")]
    [InlineData("require classification \"DIN 276\" = 330", "E408")]
    [InlineData("require material > 3", "E408")]
    public void Errors(string line, string code)
    {
        var result = CheckLine(line);

        Assert.False(result.Ok, "no error for: " + line);
        Assert.Contains(code, result.Codes());
    }

    [Theory]
    [InlineData("require property Pset_WallCommon.FireRating = \"F90\"")]
    [InlineData("require property Pset_WallCommon.LoadBearing = true")]
    [InlineData("require property Pset_WallCommon.ThermalTransmittance <= 0.24 W/m2K")]
    [InlineData("require property Qto_WallBaseQuantities.Width between 150 mm and 0.4 m")]
    [InlineData("require property Qto_WallBaseQuantities.NetSideArea > 0 m2")]
    [InlineData("require property Pset_WallCommon.Status in [\"NEW\", \"EXISTING\"]")]
    [InlineData("optional property Pset_WallCommon.Status = \"anything\"")]
    [InlineData("require property ACME.Status in [\"a\", \"b\"]")]
    [InlineData("require property ACME.Flag = true")]
    [InlineData("require property ACME.Angle datatype IfcPlaneAngleMeasure <= 45 deg")]
    [InlineData("require property ACME.Count datatype IfcCountMeasure >= 2")]
    [InlineData("require property ACME.Ratio datatype IfcReal between 0.1 and 0.9")]
    [InlineData("require property \"My Set\".\"My Prop\" matches \"A.*\"")]
    [InlineData("require property matches \"Pset_.*Common\".FireRating")]
    [InlineData("require attribute Name matches \"W-[0-9]+\"")]
    [InlineData("require attribute PredefinedType = \"SHEAR\"")]
    [InlineData("require entity IfcWall type SHEAR")]
    [InlineData("require entity only IfcWallStandardCase")]
    [InlineData("require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure")]
    [InlineData("require part of IfcSystem via IfcRelAssignsToGroup")]
    [InlineData("prohibit part of IfcWall via IfcRelAssignsToGroup")]
    [InlineData("require classification \"DIN 276\" matches \"3.*\"")]
    [InlineData("require material = \"Stahlbeton\"")]
    public void Valid_requirements_have_no_error(string line)
    {
        var result = CheckLine(line);

        Assert.True(result.Ok, result.Describe());
    }

    // The same names in a place where IDS does not check them, and what IDS allows although it is odd: warnings.
    [Theory]
    [InlineData("require property Qto_MyOwn.Thing", "W511")]
    [InlineData("prohibit property Pset_MyOwn.Thing", "W511")]
    [InlineData("prohibit property Pset_WallCommon.FireRatin", "W511")]
    [InlineData("prohibit property Pset_DoorCommon.FireRating", "W511")]
    [InlineData("optional property Pset_WallCommon.FireRating datatype IfcText", "W511")]
    [InlineData("prohibit attribute LongName", "W511")]
    [InlineData("optional attribute LongName = \"x\"", "W511")]
    [InlineData("require attribute [Name, Nope]", "W511")]
    [InlineData("require property Pset_WallCommon.Status in [\"NEW\", \"Neu\"]", "W511")]
    [InlineData("require property Qto_WallBaseQuantities.Width matches \"1.*\"", "W512")]
    [InlineData("require property Pset_WallCommon.LoadBearing matches \"t.*\"", "W512")]
    public void Warnings(string line, string code)
    {
        var result = CheckLine(line);

        Assert.True(result.Ok, result.Describe());
        Assert.Contains(code, result.Codes());
    }

    [Theory]
    [InlineData("require property A.B datatype IfcDate >= \"2026-01-01\"")]
    [InlineData("require property A.B datatype IfcDate between \"2026-01-01\" and \"2026-12-31\"")]
    [InlineData("require property A.B datatype IfcDateTime = \"2026-11-22T10:00:00\"")]
    [InlineData("require property A.B datatype IfcDuration in [\"P1D\", \"PT2H\"]")]
    [InlineData("require property A.B datatype IfcTime < \"18:00:00\"")]
    [InlineData("require property A.B datatype IfcReal = \"NaN\"")]
    [InlineData("require property A.B datatype IfcBoolean = \"1\"")]
    public void Dates_times_and_values_that_only_IDS_can_spell(string line)
    {
        var result = CheckLine(line);

        Assert.True(result.Ok, result.Describe());
    }

    [Theory]
    [InlineData("require property A.B datatype IfcDate >= \"22.11.2026\"")]
    [InlineData("require property A.B datatype IfcDate >= 2026")]
    [InlineData("require property A.B datatype IfcDuration = \"one day\"")]
    [InlineData("require property A.B datatype IfcDate length = 10")]
    [InlineData("require property A.B datatype IfcReal = \"abc\"")]
    [InlineData("require property A.B datatype IfcReal = \"5\"")]
    [InlineData("require property A.B datatype IfcBoolean = \"yes\"")]
    [InlineData("require property A.B datatype IfcBoolean = 1")]
    [InlineData("require property A.B datatype IfcBoolean length = 4")]
    public void Values_of_the_wrong_form(string line)
    {
        var result = CheckLine(line);

        Assert.Contains("E408", result.Codes());
    }

    [Fact]
    public void A_value_of_an_enumeration_of_the_standard_is_repaired_only_when_it_is_the_same_word()
    {
        // another spelling of the same word is repaired
        var cased = CheckLine("require property Pset_WallCommon.Status in [\"New\", \"EXISTING\"]");
        Assert.Contains("in [\"NEW\", \"EXISTING\"]", cased.Fixed);

        // "neu" looks like NEW, but it is a word of the requirement: the author translates it, not the checker
        var german = CheckLine("require property Pset_WallCommon.Status in [\"neu\", \"Bestand\"]");
        var error = german.Single("E408");
        Assert.Null(error.Fix);
        Assert.Contains("the value of the standard that means the same", error.Hint);
        Assert.Contains("NEW", error.Expected!);
        Assert.Contains("EXISTING", error.Expected!);
        Assert.Null(german.Fixed);

        // one value of the standard and one of the requirement: valid IDS, and still no guess
        var mixed = CheckLine("require property Pset_WallCommon.Status in [\"NEW\", \"Bestand\"]");
        Assert.True(mixed.Ok);
        Assert.Null(mixed.Single("W511").Fix);
    }

    [Fact]
    public void Classes()
    {
        Assert.Equal("ifc IFC4\n\nrule \"x\"\n  for IfcWall\n  require attribute Name", CheckRule("for IfcWal").Fixed);
        Assert.Equal("ifc IFC4\n\nrule \"x\"\n  for IfcWall\n  require attribute Name", CheckRule("for IFCWALL").Fixed);
        Assert.Contains("IfcWall", CheckRule("for Wall").Single("E401").Expected!);
        Assert.True(CheckRule("for IfcElement").Ok);

        // an abstract class named exactly selects nothing; IDS allows it
        var exact = CheckRule("for only IfcElement");
        Assert.True(exact.Ok, exact.Describe());
        Assert.Contains("W508", exact.Codes());

        // a class of another version
        var built = CheckRule("for IfcBuiltElement").Single("E401");
        Assert.Contains("IFC4X3_ADD2", built.Hint);
        Assert.True(CheckRule("for IfcBuiltElement", header: "ifc IFC4X3_ADD2").Ok);

        // a class that one version lacks works there through the subtypes that version has too
        var both = CheckRule("for IfcBuildingElement", header: "ifc IFC4, IFC4X3_ADD2");
        Assert.True(both.Ok, both.Describe());
        Assert.Contains("IFC4X3_ADD2", both.Single("W511").Message);
        Assert.True(CheckRule("for IfcBuildingElement, IfcBuiltElement", header: "ifc IFC4, IFC4X3_ADD2").Ok);
        Assert.Contains("E412", CheckRule("for IfcDoorStyle", header: "ifc IFC4, IFC4X3_ADD2").Codes());

        // a pattern is matched against the names in upper case
        Assert.True(CheckRule("for matches \"IFC.*TYPE\"").Ok);
        Assert.Contains("E401", CheckRule("for matches \"Ifc.*Type\"").Codes());
    }

    [Fact]
    public void Predefined_types()
    {
        Assert.True(CheckRule("for IfcSlab type FLOOR").Ok);
        Assert.True(CheckRule("for IfcSlab type FLOOR, ROOF").Ok);
        Assert.True(CheckRule("for IfcSlab type \"Podium\"").Ok);
        Assert.Equal("ifc IFC4\n\nrule \"x\"\n  for IfcSlab type FLOOR\n  require attribute Name", CheckRule("for IfcSlab type FLOR").Fixed);
        Assert.Equal("ifc IFC4\n\nrule \"x\"\n  for IfcSlab type FLOOR\n  require attribute Name", CheckRule("for IfcSlab type \"FLOOR\"").Fixed);
        Assert.Contains("FLOOR", CheckRule("for IfcSlab type GROUND").Single("E405").Expected!);
        Assert.Contains("E405", CheckRule("for IfcBuildingStorey type GROUND").Codes());

        // a value of one class is, for the other classes, a type that the author of the model names so
        var mixed = CheckRule("for IfcSlab, IfcWall type FLOOR");
        Assert.True(mixed.Ok, mixed.Describe());
        Assert.Contains("IfcWall", mixed.Single("W511").Message);

        // IFC2X3 walls have no predefined type at all
        Assert.Contains("E405", CheckRule("for IfcWall type \"Brandwand\"", header: "ifc IFC2X3").Codes());
    }

    [Fact]
    public void An_attribute_of_a_measure_takes_a_unit()
    {
        Assert.Contains("E409", CheckRule("for IfcBuildingStorey", "require attribute Elevation >= 3").Codes());
        Assert.True(CheckRule("for IfcBuildingStorey", "require attribute Elevation >= 3 m").Ok);
    }

    [Fact]
    public void A_name_that_means_different_things_on_different_classes_is_not_judged()
    {
        // Name is text on most classes and an enumeration on a few; without a class the value is taken as it is.
        Assert.True(LintelCompiler.Check("ifc IFC4\nrule \"x\"\n  for any\n  where attribute Name = \"Nullpunkt\"\n  require entity IfcBuildingElementProxy").Ok);
    }

    [Fact]
    public void The_values_of_a_named_list_are_checked_where_the_list_is_used()
    {
        var result = LintelCompiler.Check("ifc IFC4\ndefine Ratings = [30, 60]\nrule \"x\"\n  for IfcWall\n  require property Pset_WallCommon.FireRating in Ratings");

        var diagnostic = result.Diagnostics.First(d => d.Code == "E408");
        Assert.Equal(5, diagnostic.Span.Line);
        Assert.Null(diagnostic.Fix);
    }

    [Fact]
    public void A_number_without_a_data_type_gets_one_and_a_warning()
    {
        var result = CheckLine("require property ACME.Count >= 5");

        Assert.True(result.Ok);
        Assert.Equal("W507", Assert.Single(result.Diagnostics).Code);
    }

    [Fact]
    public void The_allowed_values_are_listed()
    {
        var property = CheckLine("require property Pset_WallCommon.FireRatin").Single("E404");
        Assert.Contains("FireRating", property.Expected!);
        Assert.Contains("LoadBearing", property.Expected!);
        Assert.Equal("Did you mean FireRating?", property.Hint);

        var unit = CheckLine("require property Qto_WallBaseQuantities.Width >= 150").Single("E409");
        Assert.Equal(new[] { "m", "cm", "mm" }, unit.Expected);

        var set = CheckLine("require property Pset_DoorCommon.FireRating").Single("E411");
        Assert.Contains("Pset_WallCommon", set.Expected!);
    }
}
