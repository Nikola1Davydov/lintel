1.
```
# Project requirements, IFC4
schema IFC4

define StatusValues = ["New", "Existing", "Demolish", "Temporary"]

# R1. All load-bearing walls must have a fire rating.
requirement R1 "All load-bearing walls must have a fire rating"
  for IfcWall where Pset_WallCommon.LoadBearing = true
  require property Pset_WallCommon.FireRating matches "^(F|REI)\d{2,3}$"

# R2. Every space must have a Name and a LongName.
requirement R2 "Every space must have a Name and a LongName"
  for IfcSpace
  require attribute Name
  require attribute LongName

# R3. Floor slabs must be between 150 mm and 400 mm thick.
requirement R3 "Floor slabs must be between 150 mm and 400 mm thick"
  for IfcSlab where PredefinedType = FLOOR
  require property Qto_SlabBaseQuantities.Width between 150 mm and 400 mm

# R4. Uniclass 2015 Ss_25 elements must be Concrete or Masonry.
requirement R4 "Elements classified Uniclass 2015 Ss_25* must have material Concrete or Masonry"
  for IfcElement where classification "Uniclass 2015" code starts with "Ss_25"
  require material name in ["Concrete", "Masonry"]

# R5. Every door must be located in a building storey.
requirement R5 "Every door must be contained in a building storey"
  for IfcDoor
  require containment in IfcBuildingStorey

# R6. Acoustic rating is optional; if present it must be one of the values.
requirement R6 "Door AcousticRating is optional, but if present must be an allowed value"
  for IfcDoor
  optional property Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB", "Rw 42 dB"]

# R7. No element may carry ACME_Old.LegacyCode.
requirement R7 "No element may carry property LegacyCode of property set ACME_Old"
  for IfcElement
  forbid property ACME_Old.LegacyCode

# R8. External wall thermal transmittance.
requirement R8 "External walls: ThermalTransmittance present, IfcThermalTransmittanceMeasure, max 0.24"
  for IfcWall where Pset_WallCommon.IsExternal = true
  require property Pset_WallCommon.ThermalTransmittance
    datatype IfcThermalTransmittanceMeasure
    value <= 0.24

# R9. No proxies.
requirement R9 "The model must not contain any IfcBuildingElementProxy"
  forbid entity IfcBuildingElementProxy

# R10. At least one storey.
requirement R10 "The model must contain at least one IfcBuildingStorey"
  require entity IfcBuildingStorey count >= 1

# R11. Structural elements need an allowed Status.
requirement R11 "Walls, columns, beams and slabs must have ACME_Common.Status with an allowed value"
  for IfcWall, IfcColumn, IfcBeam, IfcSlab
  require property ACME_Common.Status in StatusValues

# R12. Doors and windows need the same Status.
requirement R12 "Doors and windows must have ACME_Common.Status with an allowed value"
  for IfcDoor, IfcWindow
  require property ACME_Common.Status in StatusValues
```

2.
- Keywords: `requirement <ID> "<original wording>"`, `for <Entity>[, ...] where <cond>`, `require`, `optional`, `forbid`.
- Properties are referenced as `PsetName.PropName`; attributes use `require attribute Name`; the classification, material and containment clauses are separate keywords.
- Comments start with `#`; each requirement is preceded by a comment carrying the R-number.
- Operators: `=`, `<=`, `between X and Y`, `in [..]`, `matches "regex"`, `starts with`.
- Strings are double-quoted; regex is a plain quoted string (PCRE style).
- Lists are `[a, b, c]`; a shared list is defined once with `define Name = [...]` and reused by name.
- Units are written after the number (`150 mm`); unitless numbers are in the model's project units.
- "Optional" is the `optional` keyword (passes when absent); "forbidden" is `forbid`.
- Enum values such as FLOOR and entity names are bare words; booleans are `true` and `false`.
- Requirements are indentation-structured with no braces or semicolons.

3.
(a) Yes, subtypes such as IfcWallStandardCase are included.
(b) Millimetres, as written (`150 mm` and `400 mm`).
(c) Yes, a door without the property passes.
(d) W/(m²·K), the unit implied by IfcThermalTransmittanceMeasure.