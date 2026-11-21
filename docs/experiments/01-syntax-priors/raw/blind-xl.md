```
# Project information requirements R1-R12
ifc IFC4

# Allowed status values of the project, used by R11 and R12
define AllowedStatus = ["New", "Existing", "Demolish", "Temporary"]

# Valid values look like F30, F90, REI60, REI120: F or REI followed by two or three digits.
rule R1 "All load-bearing walls must have a fire rating"
  applies to IfcWall where Pset_WallCommon.LoadBearing = true
  require Pset_WallCommon.FireRating matches /^(F|REI)\d{2,3}$/

rule R2 "Every space must have a Name and a LongName"
  applies to IfcSpace
  require Name
  require LongName

# Floor slab = slab whose predefined type is FLOOR; thickness = Width in the slab base quantities.
rule R3 "Floor slabs must be between 150 mm and 400 mm thick"
  applies to IfcSlab where PredefinedType = "FLOOR"
  require Qto_SlabBaseQuantities.Width >= 150 mm
  require Qto_SlabBaseQuantities.Width <= 400 mm

rule R4 "Elements classified in Uniclass 2015 with a code starting with Ss_25 must have a material named Concrete or Masonry"
  applies to IfcElement where classification "Uniclass 2015" matches /^Ss_25.*$/
  require material in ["Concrete", "Masonry"]

# Spatial containment.
rule R5 "Every door must be located in a building storey"
  applies to IfcDoor
  require contained in IfcBuildingStorey

# Doors do not need an acoustic rating; the value is checked only when the property is present.
rule R6 "When a door has AcousticRating in Pset_DoorCommon, the value must be one of Rw 32 dB, Rw 37 dB, Rw 42 dB"
  applies to IfcDoor
  optional Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB", "Rw 42 dB"]

rule R7 "No element of any kind may carry the property LegacyCode of the property set ACME_Old"
  applies to IfcElement
  forbid ACME_Old.LegacyCode

rule R8 "For external walls, ThermalTransmittance in Pset_WallCommon must be present, have the data type IfcThermalTransmittanceMeasure and not exceed 0.24"
  applies to IfcWall where Pset_WallCommon.IsExternal = true
  require Pset_WallCommon.ThermalTransmittance: IfcThermalTransmittanceMeasure <= 0.24

rule R9 "The model must not contain any IfcBuildingElementProxy objects"
  forbid IfcBuildingElementProxy

rule R10 "The model must contain at least one IfcBuildingStorey"
  require IfcBuildingStorey

rule R11 "Every wall, column, beam and slab must have the property Status in the property set ACME_Common with an allowed status value"
  applies to IfcWall, IfcColumn, IfcBeam, IfcSlab
  require ACME_Common.Status in AllowedStatus

rule R12 "Every door and window must have the property Status in the property set ACME_Common with an allowed status value"
  applies to IfcDoor, IfcWindow
  require ACME_Common.Status in AllowedStatus
```

- `#` starts a full-line comment; the first statement `ifc IFC4` sets the schema; indentation is cosmetic and a rule ends where the next `rule` starts.
- One block per requirement: `rule <ID> "<wording>"`, with the ID as an identifier and the wording as a string (extra wording from the document goes in a comment above).
- Applicability: `applies to <IfcClass>, <IfcClass> [where <condition>]`; class names are spelled as in the schema, a comma means "or", and subtypes are included (so `IfcElement` means any element).
- A rule without `applies to` is about the model as a whole: `forbid <IfcClass>` means none, `require <IfcClass>` means at least one.
- Each requirement line starts with a modality: `require` (present and valid), `optional` (may be absent, checked when present), `forbid` (must be absent).
- References: a bare name is an IFC attribute (`Name`, `PredefinedType`); `Pset.Property` is a property, with the same form for quantities (`Qto_SlabBaseQuantities.Width`); quotes are only needed for names with spaces.
- Other facets use a keyword: `classification "<system>" <constraint on the code>`, `material <constraint on the name>`, `contained in <IfcClass>`.
- Operators: `=`, `!=`, `<`, `<=`, `>`, `>=`, `in [ ... ]`, `matches /regex/`; no operator means "exists with a value". I assumed no range keyword, so a range is two bounds, and a prefix is a regex.
- Literals: double-quoted strings, `true`/`false`, numbers with a decimal point, an optional unit after a number (`150 mm`, bare numbers are in the SI unit of the measure), regex between slashes (whole value, anchors written anyway), lists as `[a, b, c]`.
- Data type is written as `: IfcType` right after the property reference; a reusable value list is `define Name = [ ... ]`, referenced by its bare name after `in`.

(a) Yes. I assumed class names match including subtypes, so `IfcWall` covers `IfcWallStandardCase` and `IfcWallElementedCase`; R4 and R7 rely on the same assumption by using `IfcElement` as "any element".
(b) Millimetres, because the unit is written out (`150 mm`, `400 mm`) and I expect the checker to convert the model's stored value before comparing; a bare number I would expect to be read in metres (SI).
(c) Yes. `optional` means a door without the property passes; only a door that has the property with a value outside the list fails.
(d) W/(m²·K): a bare number read in the SI unit of `IfcThermalTransmittanceMeasure`.