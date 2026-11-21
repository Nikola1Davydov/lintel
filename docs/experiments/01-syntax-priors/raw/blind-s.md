1. Complete file:

```
# Information requirements for IFC4 model checking
# Source: project requirements document, IDs R1..R12 kept as requirement names.
# '#' starts a comment. Strings use double quotes. Numbers may carry a unit suffix.

define set AllowedStatus = {"New", "Existing", "Demolish", "Temporary"}

requirement R1 "All load-bearing walls must have a fire rating"
  applicable IfcWall where Pset_WallCommon.LoadBearing = true
  require Pset_WallCommon.FireRating matches "^(F|REI)[0-9]{2,3}$"
end

requirement R2 "Every space must have a Name and a LongName"
  applicable IfcSpace
  require attribute Name is present
  require attribute LongName is present
end

requirement R3 "Floor slabs must be between 150 mm and 400 mm thick"
  applicable IfcSlab where PredefinedType = FLOOR
  require Qto_SlabBaseQuantities.Width between 150 mm and 400 mm
end

requirement R4 "Uniclass 2015 Ss_25 elements must have a material Concrete or Masonry"
  applicable IfcElement where classification "Uniclass 2015" code starts with "Ss_25"
  require material name is one of {"Concrete", "Masonry"}
end

requirement R5 "Every door must be located in a building storey"
  applicable IfcDoor
  require container is IfcBuildingStorey
end

requirement R6 "A door AcousticRating, when given, must be an allowed value"
  applicable IfcDoor
  optional Pset_DoorCommon.AcousticRating is one of {"Rw 32 dB", "Rw 37 dB", "Rw 42 dB"}
end

requirement R7 "No element may carry ACME_Old.LegacyCode"
  applicable IfcProduct
  prohibit property ACME_Old.LegacyCode
end

requirement R8 "External wall ThermalTransmittance must be present, typed and at most 0.24"
  applicable IfcWall where Pset_WallCommon.IsExternal = true
  require Pset_WallCommon.ThermalTransmittance is present
  require Pset_WallCommon.ThermalTransmittance datatype IfcThermalTransmittanceMeasure
  require Pset_WallCommon.ThermalTransmittance <= 0.24 W/m2K
end

requirement R9 "The model must not contain IfcBuildingElementProxy objects"
  applicable IfcBuildingElementProxy
  prohibit
end

requirement R10 "The model must contain at least one IfcBuildingStorey"
  applicable model
  require count IfcBuildingStorey >= 1
end

requirement R11 "Walls, columns, beams and slabs must have a valid ACME Status"
  applicable IfcWall, IfcColumn, IfcBeam, IfcSlab
  require ACME_Common.Status is one of AllowedStatus
end

requirement R12 "Doors and windows must have a valid ACME Status"
  applicable IfcDoor, IfcWindow
  require ACME_Common.Status is one of AllowedStatus
end
```

2. Syntax conventions assumed:
- Each requirement is a block: `requirement <ID> "<wording>"`, then `applicable ...`, one or more `require`/`optional`/`prohibit` lines, and `end`. Keywords are lowercase.
- `applicable` takes IFC4 entity names, comma-separated as "any of", with an optional `where` filter. `applicable model` means the whole project.
- Subtypes are included by inheritance, so `IfcWall` also covers `IfcWallStandardCase`.
- Properties and quantities are referenced as `PsetOrQset.PropertyName`. Attributes use `attribute Name`. Entity-level checks use `container is` and `count ... >=`.
- Classifications use `classification "<system>" code starts with "<prefix>"`. Materials use `material name is one of {...}`.
- Operators are `=`, `<=`, `>=`, `between X and Y` (inclusive), `is present`, `is one of`, `matches "<regex>"`, and `datatype <Name>`.
- Named sets are declared once with `define set Name = {...}` and reused by name. Literal lists use braces.
- Numbers may carry a unit suffix (`mm`, `W/m2K`). A number with no suffix is in the IFC SI base unit.
- `optional` passes when the property is absent and otherwise checks its value. `prohibit` fails when the matched item exists. Regex is ECMAScript-style inside double quotes.

3. One-line answers:
(a) Yes. The statement on IfcWall also applies to subtypes such as IfcWallStandardCase, by inheritance.
(b) Millimetres, because the unit is written explicitly as `mm` in the R3 statement.
(c) Yes, it passes. `optional` lets an absent property through and checks only a present value.
(d) W/(m²·K), written as `W/m2K`, which is IFC's SI unit for thermal transmittance.