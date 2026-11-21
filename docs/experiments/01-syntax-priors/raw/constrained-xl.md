```
# Project information requirements, statements R1-R12 from the requirements document
title "Project information requirements"
ifc IFC4

# Class names match exactly, so the IFC4 subtypes (...StandardCase, ...ElementedCase) are listed explicitly.

# Allowed status values, defined once and reused in R11 and R12
define StatusValues = "New", "Existing", "Demolish", "Temporary"

# R1. All load-bearing walls must have a fire rating. Valid values look like F30, F90, REI60, REI120 (F or REI followed by two or three digits). Use the standard IFC property set for walls.
R1: IfcWall, IfcWallStandardCase, IfcWallElementedCase with property Pset_WallCommon.LoadBearing = true must have property Pset_WallCommon.FireRating matches "(F|REI)[0-9]{2,3}"

# R2. Every space must have a Name and a LongName.
R2: IfcSpace must have attribute Name and attribute LongName

# R3. Floor slabs (slabs whose predefined type is FLOOR) must be between 150 mm and 400 mm thick (Width in the slab base quantities).
# Lengths are written in metres: 150 mm = 0.15, 400 mm = 0.4.
R3: IfcSlab, IfcSlabStandardCase, IfcSlabElementedCase type FLOOR must have property Qto_SlabBaseQuantities.Width >= 0.15 and <= 0.4

# R4. Elements classified in "Uniclass 2015" with a code starting with "Ss_25" must have a material named "Concrete" or "Masonry".
R4: element with classification "Uniclass 2015" matches "Ss_25.*" must have material in "Concrete", "Masonry"

# R5. Every door must be located in a building storey (spatial containment).
R5: IfcDoor, IfcDoorStandardCase must be part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

# R6. Doors do not need an acoustic rating, but when a door has AcousticRating in Pset_DoorCommon, the value must be one of "Rw 32 dB", "Rw 37 dB", "Rw 42 dB".
R6: IfcDoor, IfcDoorStandardCase may have property Pset_DoorCommon.AcousticRating in "Rw 32 dB", "Rw 37 dB", "Rw 42 dB"

# R7. No element of any kind may carry the property "LegacyCode" of the property set "ACME_Old".
R7: element with property ACME_Old.LegacyCode must not exist

# R8. For external walls, ThermalTransmittance in Pset_WallCommon must be present, must have the data type IfcThermalTransmittanceMeasure, and must not exceed 0.24.
R8: IfcWall, IfcWallStandardCase, IfcWallElementedCase with property Pset_WallCommon.IsExternal = true must have property Pset_WallCommon.ThermalTransmittance as IfcThermalTransmittanceMeasure <= 0.24

# R9. The model must not contain any IfcBuildingElementProxy objects.
R9: IfcBuildingElementProxy must not exist

# R10. The model must contain at least one IfcBuildingStorey.
R10: IfcBuildingStorey must exist

# R11. Every wall, column, beam and slab must have the property Status in the property set "ACME_Common" with one of the project's allowed status values: New, Existing, Demolish, Temporary.
R11: IfcWall, IfcWallStandardCase, IfcWallElementedCase, IfcColumn, IfcColumnStandardCase, IfcBeam, IfcBeamStandardCase, IfcSlab, IfcSlabStandardCase, IfcSlabElementedCase must have property ACME_Common.Status in StatusValues

# R12. Every door and window must have the same Status property with one of the same allowed status values (define the allowed values once and reuse them).
R12: IfcDoor, IfcDoorStandardCase, IfcWindow, IfcWindowStandardCase must have property ACME_Common.Status in StatusValues
```

- Line shape: `ID: <IfcClass list | element> [type X] [with <facet> and <facet>] <modal> <facet> and <facet>`; the `ID:` label is the specification name; header lines are `title "..."` and `ifc IFC4`.
- Comments: whole lines starting with `#` (they carry the original wording); no trailing comments.
- Cardinality by modal: `must have` = required, `may have` = optional, `must not have` = prohibited facet; at model level `must exist` (at least one) and `must not exist` (none).
- Classes: IFC names in CamelCase, exact match; `type FLOOR` = predefined type; `element` = any class and is always followed by `with`.
- Facets by keyword: `attribute Name`, `property Pset.Property` (also for Qto sets), `classification "System"` + restriction on the code, `material` + restriction on the name, `part of IfcClass via IfcRel...` (after `must be`).
- Data type: `as IfcXxx` directly after the property name.
- Operators: `=` exact value, `in` enumeration, `matches` pattern, `>=` `<=` `>` `<` bounds (two bounds joined by `and`), `length` plus a comparison for text length.
- Strings in double quotes; IFC identifiers, numbers and `true`/`false` unquoted; patterns are quoted XSD regex that must match the whole value (no `^`/`$`, no `/.../`).
- Units: bare numbers with a decimal point, no unit suffixes; always SI units as in IDS.
- Lists: comma-separated without brackets; a named list is `define Name = a, b, c`, used by its bare name after `in`.

(a) No — class names match exactly as in IDS 1.0 (no inheritance), which is why I listed the IFC4 subtypes such as IfcWallStandardCase explicitly.
(b) Metres (SI, independent of the model's length unit), so 150 mm and 400 mm are written 0.15 and 0.4.
(c) Yes — `may have` is optional cardinality: a door without the property passes; a door with a value outside the list fails.
(d) W/(m²·K), the SI unit of IfcThermalTransmittanceMeasure.