using Xunit;

namespace Lintel.Tests;

/// <summary>
/// The spellings below are what language models wrote when they were asked to guess the syntax without a
/// specification (docs/experiments/01-syntax-priors). One check turns such a file into valid Lintel.
/// </summary>
public class FixTests
{
    [Fact]
    public void A_file_in_the_guessed_dialect_is_repaired_in_one_check()
    {
        const string guessed = """
            schema IFC4

            define Status = ["New", "Existing"]

            requirement R1 "Fire rating"
              applies to IfcWall where Pset_WallCommon.LoadBearing == true
              require Pset_WallCommon.FireRating matches /^(F|REI)\d{2,3}$/

            rule R3 "Slabs"
              for IfcSlab where PredefinedType = FLOOR
              require property Qto_SlabBaseQuantities.Width >= 150 mm and <= 400 mm
              require attribute Name is present and attribute Description exists

            rule R6 "Doors"
              for IfcDoor
              optional property Pset_DoorCommon.AcousticRating is one of ["Rw 32 dB", "Rw 37 dB"]
              forbid property ACME_Old.LegacyCode
              require contained in IfcBuildingStorey
              require classification "Uniclass 2015" code starts with "Ss_25"
              require property ACME.Status in Statuss
            """;

        const string repaired = """
            ifc IFC4

            define Status = ["New", "Existing"]

            rule R1 "Fire rating"
              for IfcWall where property Pset_WallCommon.LoadBearing = true
              require property Pset_WallCommon.FireRating matches "(F|REI)\d{2,3}"

            rule R3 "Slabs"
              for IfcSlab type FLOOR
              require property Qto_SlabBaseQuantities.Width between 150 mm and 400 mm
              require attribute Name
              require attribute Description

            rule R6 "Doors"
              for IfcDoor
              optional property Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB"]
              prohibit property ACME_Old.LegacyCode
              require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure
              require classification "Uniclass 2015" matches "Ss_25.*"
              require property ACME.Status in Status
            """;

        var result = LintelCompiler.Check(guessed);

        Assert.False(result.Ok);
        Assert.Equal(repaired, result.Fixed);
        Assert.True(result.FixedOk);
        Assert.Empty(LintelCompiler.Check(repaired).Diagnostics);
    }

    [Fact]
    public void A_clean_file_has_nothing_to_fix()
    {
        var result = LintelCompiler.Check("ifc IFC4\nrule \"x\"\n  for IfcWall\n  require attribute Name");

        Assert.True(result.Ok);
        Assert.Null(result.Fixed);
        Assert.Null(result.FixedOk);
    }
}
