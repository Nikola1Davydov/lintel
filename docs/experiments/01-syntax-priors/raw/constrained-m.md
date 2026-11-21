1.
```
# Project requirements, IFC4
ifc-version IFC4

define-list Status = "New", "Existing", "Demolish", "Temporary"

# R1 All load-bearing walls must have a fire rating.
R1
applies-to entity IfcWall
applies-to property Pset_WallCommon LoadBearing = TRUE
requires property Pset_WallCommon FireRating matches "(F|REI)[0-9]{2,3}" required

# R2 Every space must have a Name and a LongName.
R2a
applies-to entity IfcSpace
requires attribute Name required
R2b
applies-to entity IfcSpace
requires attribute LongName required

# R3 Floor slabs must be between 150 mm and 400 mm thick.
R3
applies-to entity IfcSlab predefined FLOOR
requires property Qto_SlabBaseQuantities Width between 150 and 400 required

# R4 Uniclass Ss_25 elements must have material Concrete or Masonry.
R4
applies-to classification "Uniclass 2015" matches "Ss_25.*"
requires material in "Concrete", "Masonry"

# R5 Every door must be located in a building storey.
R5
applies-to entity IfcDoor
requires partof IfcBuildingStorey

# R6 Acoustic rating of doors is optional but constrained.
R6
applies-to entity IfcDoor
requires property Pset_DoorCommon AcousticRating in "Rw 32 dB", "Rw 37 dB", "Rw 42 dB" optional

# R7 No element may carry ACME_Old.LegacyCode.
R7
requires property ACME_Old LegacyCode prohibited

# R8 External walls thermal transmittance.
R8
applies-to entity IfcWall
applies-to property Pset_WallCommon IsExternal = TRUE
requires property Pset_WallCommon ThermalTransmittance datatype IfcThermalTransmittanceMeasure max 0.24 required

# R9 No building element proxies.
R9
applies-to entity IfcBuildingElementProxy
requires entity IfcBuildingElementProxy prohibited

# R10 At least one building storey.
R10
requires entity IfcBuildingStorey required

# R11 Structural elements need Status.
R11a
applies-to entity IfcWall
requires property ACME_Common Status in list Status required
R11b
applies-to entity IfcColumn
requires property ACME_Common Status in list Status required
R11c
applies-to entity IfcBeam
requires property ACME_Common Status in list Status required
R11d
applies-to entity IfcSlab
requires property ACME_Common Status in list Status required

# R12 Doors and windows need Status.
R12a
applies-to entity IfcDoor
requires property ACME_Common Status in list Status required
R12b
applies-to entity IfcWindow
requires property ACME_Common Status in list Status required
```

2.
- Statements are grouped under an ID line (R1...), followed by `applies-to` (applicability) and `requires` (requirements) lines; one facet per line.
- Facet keywords: entity, attribute, property, classification, material, partof; property is written as `property <Pset> <Name>`.
- Entity names are IFC class names in uppercase-camel form (IfcWall); `predefined X` follows the entity.
- Cardinality is a trailing word: required, optional, prohibited.
- Operators are words: `=`, `in "a", "b"`, `matches "regex"`, `between A and B`, `max`/`min`, `datatype`.
- Strings are double-quoted; regex is XSD-style in quotes; lists are comma-separated, with a named list defined by `define-list`.
- Comments start with `#`; the requirement wording is kept as a comment above its ID.
- No units in the syntax; numbers are bare.
- A statement with several entities is split into one ID per entity (R2a/R2b, R11a-d).

3.
(a) Yes, IfcWall also matches subtypes such as IfcWallStandardCase (IDS entity matching is subtype-inclusive).
(b) Millimetres, as I assumed bare numbers in the IFC model's length unit (project unit, mm here), so 150 and 400 mean mm.
(c) Yes, a door without the property passes, because it is marked optional.
(d) W/(m2.K), the unit of IfcThermalTransmittanceMeasure (SI), with the number taken as-is.