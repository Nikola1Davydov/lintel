1. File

```
# ACME project information requirements, IFC4
# Source: project requirements document, items R1-R12
SCHEMA IFC4

LIST StatusValues = "New", "Existing", "Demolish", "Temporary"

# R1 All load-bearing walls must have a fire rating. Valid values look like F30, F90, REI60, REI120 (F or REI followed by two or three digits).
R1 FOR IfcWall WHERE PROPERTY Pset_WallCommon.LoadBearing IS TRUE REQUIRE PROPERTY Pset_WallCommon.FireRating MATCHES "^(F|REI)[0-9]{2,3}$"

# R2 Every space must have a Name and a LongName.
R2 FOR IfcSpace REQUIRE ATTRIBUTE Name
R2 FOR IfcSpace REQUIRE ATTRIBUTE LongName

# R3 Floor slabs (slabs whose predefined type is FLOOR) must be between 150 mm and 400 mm thick (Width in the slab base quantities).
R3 FOR IfcSlab WHERE ATTRIBUTE PredefinedType IS "FLOOR" REQUIRE PROPERTY Qto_SlabBaseQuantities.Width BETWEEN 150 AND 400 MM

# R4 Elements classified in "Uniclass 2015" with a code starting with "Ss_25" must have a material named "Concrete" or "Masonry".
R4 FOR ANY WHERE CLASSIFICATION "Uniclass 2015" CODE STARTS WITH "Ss_25" REQUIRE MATERIAL NAME IN "Concrete", "Masonry"

# R5 Every door must be located in a building storey (spatial containment).
R5 FOR IfcDoor REQUIRE PART OF IfcBuildingStorey

# R6 Doors do not need an acoustic rating, but when a door has AcousticRating in Pset_DoorCommon, the value must be one of "Rw 32 dB", "Rw 37 dB", "Rw 42 dB".
R6 FOR IfcDoor OPTIONAL PROPERTY Pset_DoorCommon.AcousticRating IN "Rw 32 dB", "Rw 37 dB", "Rw 42 dB"

# R7 No element of any kind may carry the property "LegacyCode" of the property set "ACME_Old".
R7 FOR ANY PROHIBIT PROPERTY ACME_Old.LegacyCode

# R8 For external walls, ThermalTransmittance in Pset_WallCommon must be present, must have the data type IfcThermalTransmittanceMeasure, and must not exceed 0.24.
R8 FOR IfcWall WHERE PROPERTY Pset_WallCommon.IsExternal IS TRUE REQUIRE PROPERTY Pset_WallCommon.ThermalTransmittance OF TYPE IfcThermalTransmittanceMeasure AT MOST 0.24

# R9 The model must not contain any IfcBuildingElementProxy objects.
R9 FOR ANY PROHIBIT ENTITY IfcBuildingElementProxy

# R10 The model must contain at least one IfcBuildingStorey.
R10 MODEL REQUIRE ENTITY IfcBuildingStorey AT LEAST 1

# R11 Every wall, column, beam and slab must have the property Status in the property set "ACME_Common" with one of the project's allowed status values: New, Existing, Demolish, Temporary.
R11 FOR IfcWall OR IfcColumn OR IfcBeam OR IfcSlab REQUIRE PROPERTY ACME_Common.Status IN LIST StatusValues

# R12 Every door and window must have the same Status property with one of the same allowed status values (define the allowed values once and reuse them).
R12 FOR IfcDoor OR IfcWindow REQUIRE PROPERTY ACME_Common.Status IN LIST StatusValues
```

2. Syntax conventions assumed

- The file opens with `SCHEMA IFC4`. Statements are one per line, `#` starts a comment, and the requirement wording goes in a comment above its statement.
- Each statement starts with its requirement ID (R1 to R12). An ID may repeat on several lines when a requirement has several checks (R2).
- `FOR <entity>` sets the applicability, `FOR ANY` means every element, and `OR` joins entity names. A statement about an entity also applies to its subtypes, so IfcWallStandardCase is included.
- `WHERE` adds applicability filters. A statement without `WHERE` applies to everything its `FOR` matches.
- Facets: `ATTRIBUTE <Name>`, `PROPERTY <PropertySet>.<Name>`, `CLASSIFICATION "<system>" CODE STARTS WITH "<prefix>"`, `MATERIAL NAME`, `PART OF <entity>`, `ENTITY <name>`.
- Cardinality keywords: `REQUIRE` (required), `OPTIONAL` (checked only if present), `PROHIBIT` (must not be present). `OPTIONAL` is how "optional" is written.
- Value keywords: `IS` (exact value), `IN` (enumeration, inline or `IN LIST name` for a list defined once with `LIST`), `MATCHES "<regex>"` (XML Schema regex), `BETWEEN a AND b` (inclusive), `AT MOST` / `AT LEAST` (one-sided bounds), `OF TYPE <IFC data type>`.
- Strings are double-quoted and lists are comma-separated. Booleans are `TRUE` / `FALSE`. Keywords are uppercase.
- Numbers take an optional unit suffix (`MM`, `M`). Without a suffix they are in the IFC schema's unit for that attribute, so 0.24 is W/(m²·K).
- Model-level counts (R10) are not native in IDS 1.0. The `MODEL REQUIRE ENTITY ... AT LEAST n` form is my assumption, and the compiler would have to emit a count-based specification for it.

3. Answers

(a) Yes. A statement about IfcWall also applies to IfcWallStandardCase, because subtypes are included by default.
(b) Millimetres, because of the `MM` suffix. The checker converts them to the model's metre-based length.
(c) Yes. Under `OPTIONAL`, a door without the property passes.
(d) W/(m²·K), the IFC unit of IfcThermalTransmittanceMeasure. The number has no suffix, so it is taken in that unit.