1. File

```
# Project information requirements, checked against IFC4
ids "ACME Project Information Requirements"
ifc IFC4

define AllowedStatus = ["New", "Existing", "Demolish", "Temporary"]

spec R1 "All load-bearing walls must have a fire rating."
for entity IfcWall
where property Pset_WallCommon.LoadBearing = true
require property Pset_WallCommon.FireRating matches "(F|REI)[0-9]{2,3}"

spec R2 "Every space must have a Name and a LongName."
for entity IfcSpace
require attribute Name
require attribute LongName

spec R3 "Floor slabs must be between 150 mm and 400 mm thick."
for entity IfcSlab with predefined type FLOOR
# values are in SI units (metres): 150 mm .. 400 mm
require property Qto_SlabBaseQuantities.Width between 0.15 and 0.4

spec R4 "Uniclass 2015 Ss_25 elements must be concrete or masonry."
for classification "Uniclass 2015" code matches "Ss_25.*"
require material in ["Concrete", "Masonry"]

spec R5 "Every door must be located in a building storey."
for entity IfcDoor
require part of IfcBuildingStorey via IfcRelContainedInSpatialStructure

spec R6 "Doors do not need an acoustic rating; if present it must be an allowed value."
for entity IfcDoor
optional property Pset_DoorCommon.AcousticRating in ["Rw 32 dB", "Rw 37 dB", "Rw 42 dB"]

spec R7 "No element may carry ACME_Old.LegacyCode."
for property ACME_Old.LegacyCode
expect none

spec R8 "External walls: ThermalTransmittance present, IfcThermalTransmittanceMeasure, max 0.24."
for entity IfcWall
where property Pset_WallCommon.IsExternal = true
# value in SI units: W/(m2·K)
require property Pset_WallCommon.ThermalTransmittance type IfcThermalTransmittanceMeasure <= 0.24

spec R9 "The model must not contain any IfcBuildingElementProxy objects."
for entity IfcBuildingElementProxy
expect none

spec R10 "The model must contain at least one IfcBuildingStorey."
for entity IfcBuildingStorey
expect at least 1

spec R11 "Walls, columns, beams and slabs must have an allowed Status."
for entity IfcWall, IfcColumn, IfcBeam, IfcSlab
require property ACME_Common.Status in AllowedStatus

spec R12 "Doors and windows must have an allowed Status."
for entity IfcDoor, IfcWindow
require property ACME_Common.Status in AllowedStatus
```

2. Conventions I assumed
- Header lines: `ids "<title>"` and `ifc IFC4`. Each requirement starts with `spec <ID> "<original wording>"` and runs until the next `spec`. Comments start with `#`.
- Applicability: `for entity …` (or `for property` / `for classification`), filtered by `where …`. Requirements: `require …` (required), `optional …` (may be missing, but if present it must be valid). Forbidden: `prohibit …` for a facet, `expect none` for the whole spec (maps to maxOccurs=0), and `expect at least 1` for minOccurs.
- Facet references: `attribute Name`, `property Pset.Name` (dotted, with the set name quoted if it has spaces), `classification "<system>" code …`, `material …`, `part of <Entity> via <Relation>`, `entity X with predefined type Y`.
- Value restrictions: `= value`, `in [ … ]` for an enumeration, `matches "<XSD regex>"` (no ^/$, anchored implicitly as in IDS), `between a and b` (inclusive), `<=`, `>=`, `<`, `>`, `length …`.
- `type <IfcMeasure>` sets the IDS dataType of a property or attribute.
- Strings go in double quotes; IFC names, booleans (`true`/`false`) and numbers are bare.
- Lists use `[ "a", "b" ]`. `define Name = [ … ]` declares a reusable named enumeration that is referenced by name.
- Several entities after `for entity` (comma-separated) compile to an enumeration on the entity name.
- Numbers carry no unit suffix. They are taken in IFC/IDS SI base units, so comments note the original units.
- Entity names are case-insensitive (IfcWall = IFCWALL).

3. Answers
(a) No. As in IDS 1.0, the entity facet matches exactly that class, so subtypes would have to be listed.
(b) Metres, the SI base unit (0.15 to 0.4).
(c) Yes. `optional` only checks the value when the property exists.
(d) W/(m²·K), the SI unit.