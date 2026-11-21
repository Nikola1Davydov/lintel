1. File

```
# ACME project – information requirements
# Source: Project Requirements Document
schema IFC4

define StatusValues = ["New", "Existing", "Demolish", "Temporary"]

# R1. All load-bearing walls must have a fire rating.
requirement R1 "All load-bearing walls must have a fire rating"
  applies to IfcWall where Pset_WallCommon.LoadBearing = true
  require Pset_WallCommon.FireRating matches /^(F|REI)[0-9]{2,3}$/

# R2. Every space must have a Name and a LongName.
requirement R2 "Every space must have a Name and a LongName"
  applies to IfcSpace
  require attribute Name exists
  require attribute LongName exists

# R3. Floor slabs must be between 150 mm and 400 mm thick.
requirement R3 "Floor slabs must be between 150 mm and 400 mm thick"
  applies to IfcSlab where PredefinedType = FLOOR
  require Qto_SlabBaseQuantities.Width between 150 mm and 400 mm

# R4. Uniclass Ss_25 elements must be Concrete or Masonry.
requirement R4 "Elements classified Ss_25* must have material Concrete or Masonry"
  applies to any where classification "Uniclass 2015" matches /^Ss_25/
  require material in ["Concrete", "Masonry"]

# R5. Every door must be located in a building storey.
requirement R5 "Every door must be contained in a building storey"
  applies to IfcDoor
  require contained in IfcBuildingStorey

# R6. AcousticRating is optional, but if present must be an allowed value.
requirement R6 "Door AcousticRating, if present, must be an allowed value"
  applies to IfcDoor
  optional Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB", "Rw 42 dB"]

# R7. No element may carry ACME_Old.LegacyCode.
requirement R7 "No element may carry ACME_Old.LegacyCode"
  applies to any
  prohibit ACME_Old.LegacyCode

# R8. External walls: ThermalTransmittance present, typed, <= 0.24.
requirement R8 "External walls: U-value present and <= 0.24 W/(m2K)"
  applies to IfcWall where Pset_WallCommon.IsExternal = true
  require Pset_WallCommon.ThermalTransmittance type IfcThermalTransmittanceMeasure <= 0.24 W/(m2.K)

# R9. No IfcBuildingElementProxy objects.
requirement R9 "The model must not contain IfcBuildingElementProxy"
  applies to IfcBuildingElementProxy
  prohibit entity

# R10. At least one IfcBuildingStorey.
requirement R10 "The model must contain at least one building storey"
  applies to IfcBuildingStorey
  count >= 1

# R11. Status for walls, columns, beams, slabs.
requirement R11 "Walls, columns, beams and slabs must have an allowed Status"
  applies to IfcWall, IfcColumn, IfcBeam, IfcSlab
  require ACME_Common.Status in StatusValues

# R12. Status for doors and windows.
requirement R12 "Doors and windows must have an allowed Status"
  applies to IfcDoor, IfcWindow
  require ACME_Common.Status in StatusValues
```

2. Conventions I assumed
- Each rule is a `requirement <ID> "<wording>"` block. `#` starts a comment, and `schema IFC4` is the file header.
- `applies to <Entity>[, ...] where <condition>` sets which elements a rule covers. `any` means every element.
- A property is written `PsetName.PropertyName` (dot notation). An IFC attribute is written `attribute Name`, or bare, as in `PredefinedType`.
- `require` means the value must be there and must pass the check. `optional` means it may be missing, but if it is there it must pass. `prohibit` means it must not be there.
- Comparisons use `=`, `<=`, `>=`, `in [list]`, `between a and b`, and `matches /regex/`.
- Strings go in double quotes and regular expressions between slashes. Enum values such as `FLOOR` and booleans such as `true` are bare.
- Lists use square brackets. `define Name = [...]` sets up a reusable value list, which is then referenced by name.
- `classification "<system>" matches ...` checks a classification and `material in [...]` checks a material name.
- `contained in <Entity>` checks spatial containment. `count >= n` checks a model-wide quantity.
- A number can carry an explicit unit (`150 mm`, `W/(m2.K)`), and the checker converts the model's values to it. `type <IfcMeasure>` checks a value's data type.

3. Answers
(a) Yes. `applies to IfcWall` also covers subtypes such as IfcWallStandardCase.
(b) Millimetres, because the unit is written next to the number. The checker converts the model's length unit to match.
(c) Yes. Because the property is `optional`, a door without it passes.
(d) W/(m²·K), which is the SI unit of IfcThermalTransmittanceMeasure. I also wrote it out explicitly.